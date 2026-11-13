using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Time
{
    /// <summary>
    /// Pass IV (Worker 1): time correctness — short-tick overflow is deferred (never
    /// discarded) and collapsed day rollovers run every crossed day's drivers.
    /// Drives TimeManager's private clock through reflection (white-box coverage of
    /// the exact defect sites); public behavior is asserted throughout.
    /// </summary>
    [TestFixture]
    public sealed class TimeCorrectnessTests
    {
        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = typeof(TimeManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "TimeManager field '" + name + "' must exist");
            field.SetValue(target, value);
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = typeof(TimeManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "TimeManager field '" + name + "' must exist");
            return (T)field.GetValue(target);
        }

        private static void InvokePrivate(object target, string name, params object[] args)
        {
            MethodInfo method = typeof(TimeManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "TimeManager method '" + name + "' must exist");
            method.Invoke(target, args);
        }

        private TimeManager CreateTimeManager(int maxShortTicksPerFrame = 64, int maxDayRolloversPerFrame = 7)
        {
            var manager = new TimeManager();
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            settings.shortTickIntervalGameMinutes = 15f; // 900 game-seconds per tick
            settings.maxShortTicksPerFrame = maxShortTicksPerFrame;
            settings.maxDayRolloversPerFrame = maxDayRolloversPerFrame;
            settings.Sanitize();
            SetField(manager, "settings", settings);
            SetField(manager, "gameSecondsIntoDay", 0f);
            SetField(manager, "absoluteDayIndex", 0);
            SetField(manager, "shortTickAccumulatorGameSeconds", 0f);
            SetField(manager, "shortTickIndex", 0);
            return manager;
        }

        [Test]
        public void ShortTickOverflow_IsDeferred_NotDiscarded()
        {
            TimeManager manager = CreateTimeManager();
            float interval = 900f;

            // 100 ticks of accumulated time; the per-frame cap is 64.
            SetField(manager, "absoluteDayIndex", 1);
            SetField(manager, "gameSecondsIntoDay", 3600f);
            SetField(manager, "shortTickAccumulatorGameSeconds", 100f * interval);
            int emitted = 0;
            manager.ShortTick += _ => emitted++;

            InvokePrivate(manager, "ProcessShortTicks");

            Assert.AreEqual(64, emitted, "The cap limits emitted intervals, not a per-day index.");
            Assert.AreEqual(36f * interval, GetField<float>(manager, "shortTickAccumulatorGameSeconds"), 0.001f,
                "overflow seconds must be deferred to later frames, never discarded");
            Assert.AreEqual(36, manager.CompressedShortTicksLastFrame);
        }

        [Test]
        public void SimulationTimeAdvanced_ReportsOnlyClockProcessedSeconds()
        {
            TimeManager manager = CreateTimeManager();
            var increments = new List<float>();
            manager.SimulationTimeAdvanced += context => increments.Add(context.GameDeltaSeconds);

            manager.AdvanceForValidation(2000f);

            Assert.AreEqual(1, increments.Count,
                "A single authoritative clock advancement publishes one matching movement delta.");
            Assert.AreEqual(2000f, increments[0], 0.001f,
                "Consumers receive processed game seconds, not a separately scaled render delta.");
        }

        [Test]
        public void ScenarioStartBeforeCalendarEpoch_UsesPositiveCampaignDaysAndRestoresDate()
        {
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            var root = new GameObject("Scenario Date Test Clock");
            try
            {
                settings.startingTimeOfDay01 = 0f;
                TimeManager manager = root.AddComponent<TimeManager>();
                SetField(manager, "settings", settings);

                Assert.IsTrue(manager.StartNewScenarioAtDate(1870, 1, 1));
                Assert.AreEqual(1870, manager.CurrentDate.Year);
                Assert.AreEqual(1, manager.CurrentDate.Month);
                Assert.AreEqual(1, manager.CurrentDate.DayOfMonth);
                Assert.AreEqual(1870, manager.TimelineAnchor.Year,
                    "Calendar-based population and finance consumers must share the scenario epoch.");
                Assert.AreEqual(1, HouseholdHeatingEvaluator.GetMonthFromAbsoluteDay(
                    manager.CurrentAbsoluteDayIndex, manager.TimelineAnchor),
                    "A January scenario start must remain winter to calendar-dependent simulation systems.");
                Assert.AreEqual(0, manager.CurrentAbsoluteDayIndex,
                    "Scenario event indices remain non-negative even when the Gregorian date predates the shared epoch.");

                TimeSaveDto save = manager.CaptureSaveDto();
                Assert.Less(save.calendarDayOffset, 0, "The pre-epoch calendar offset must be preserved in saves.");
                manager.AdvanceForValidation(settings.GameSecondsPerDay);
                Assert.AreEqual(2, manager.CurrentDate.DayOfMonth);
                Assert.AreEqual(1, manager.CurrentAbsoluteDayIndex);

                manager.LoadFromSaveDto(save);
                Assert.AreEqual(1870, manager.CurrentDate.Year);
                Assert.AreEqual(1, manager.CurrentDate.Month);
                Assert.AreEqual(1, manager.CurrentDate.DayOfMonth);
                Assert.AreEqual(1870, manager.TimelineAnchor.Year,
                    "The active calendar epoch must survive save/load with the scenario date.");
                Assert.AreEqual(0, manager.CurrentAbsoluteDayIndex);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ShortTickOverflow_DrainsAcrossFrames()
        {
            TimeManager manager = CreateTimeManager();
            float interval = 900f;

            SetField(manager, "absoluteDayIndex", 1);
            SetField(manager, "gameSecondsIntoDay", 3600f);
            SetField(manager, "shortTickAccumulatorGameSeconds", 100f * interval);
            int emitted = 0;
            manager.ShortTick += _ => emitted++;
            InvokePrivate(manager, "ProcessShortTicks"); // 64 processed, 36 deferred
            InvokePrivate(manager, "ProcessShortTicks"); // 36 processed, 0 deferred

            Assert.AreEqual(100, emitted, "All deferred intervals must eventually be emitted.");
            Assert.AreEqual(4, manager.ShortTickIndex, "The last interval ends at 01:00 on day 1.");
            Assert.AreEqual(0f, GetField<float>(manager, "shortTickAccumulatorGameSeconds"), 0.001f);
        }

        [Test]
        public void ShortTickBurst_ReportsEachReservedWorkWindowOnce()
        {
            TimeManager manager = CreateTimeManager();
            const float interval = 900f;
            SetField(manager, "gameSecondsIntoDay", 3f * interval);
            SetField(manager, "shortTickAccumulatorGameSeconds", 3f * interval);
            InvokePrivate(manager, "RefreshTimeOfDay01FromCurrentSeconds");
            var ticks = new List<SimulationTickContext>();
            manager.ShortTick += ticks.Add;

            InvokePrivate(manager, "ProcessShortTicks");

            Assert.AreEqual(3, ticks.Count);
            Assert.AreEqual(15f / 1440f, ticks[0].TimeOfDay01, 0.000001f);
            Assert.AreEqual(30f / 1440f, ticks[1].TimeOfDay01, 0.000001f);
            Assert.AreEqual(45f / 1440f, ticks[2].TimeOfDay01, 0.000001f);
            Assert.AreEqual(15f * 60f, ticks[0].GameSecondsIntoDay, 0.001f);
            Assert.AreEqual(30f * 60f, ticks[1].GameSecondsIntoDay, 0.001f);
            Assert.AreEqual(45f * 60f, ticks[2].GameSecondsIntoDay, 0.001f);
            Assert.AreEqual(1, ticks[0].TickIndex);
            Assert.AreEqual(2, ticks[1].TickIndex);
            Assert.AreEqual(3, ticks[2].TickIndex);
            Assert.AreEqual(0, ticks[0].Date.AbsoluteDayIndex);
            Assert.AreEqual(0, ticks[1].Date.AbsoluteDayIndex);
            Assert.AreEqual(0, ticks[2].Date.AbsoluteDayIndex);
        }

        [Test]
        public void ShortTickBurst_ConsumersSeeChronologicalClockAtEachBoundary()
        {
            TimeManager manager = CreateTimeManager();
            SimulationTimeSettings settings = GetField<SimulationTimeSettings>(manager, "settings");
            settings.startingTimeOfDay01 = 0f;
            manager.AdvanceForValidation(0f);
            float interval = settings.ShortTickIntervalGameSeconds;
            double startAbsoluteSeconds = manager.PreciseAbsoluteGameSeconds;
            var observed = new List<double>();
            var dispatchOrder = new List<string>();
            manager.BeforeShortTick += context => dispatchOrder.Add("before-" + context.TickIndex);
            manager.ShortTick += context =>
            {
                dispatchOrder.Add("after-" + context.TickIndex);
                double expected = (double)context.Date.AbsoluteDayIndex * settings.GameSecondsPerDay
                    + context.GameSecondsIntoDay;
                Assert.AreEqual(expected, manager.PreciseAbsoluteGameSeconds, 0.001d,
                    "Clock getters inside a short-tick callback must describe that tick, not the frame endpoint.");
                Assert.AreEqual(context.Date, manager.CurrentDate);
                Assert.AreEqual(context.TimeOfDay01, manager.TimeOfDay01, 0.000001f);
                observed.Add(manager.PreciseAbsoluteGameSeconds);
            };

            manager.AdvanceForValidation(interval * 4f);

            Assert.AreEqual(4, observed.Count);
            Assert.AreEqual(8, dispatchOrder.Count);
            for (int i = 0; i < dispatchOrder.Count; i += 2)
            {
                StringAssert.StartsWith("before-", dispatchOrder[i]);
                StringAssert.StartsWith("after-", dispatchOrder[i + 1]);
                Assert.AreEqual(dispatchOrder[i].Substring("before-".Length),
                    dispatchOrder[i + 1].Substring("after-".Length));
            }
            for (int i = 1; i < observed.Count; i++)
                Assert.AreEqual(interval, observed[i] - observed[i - 1], 0.001d,
                    "Ticks in one catch-up burst must be observed in chronological order.");
            Assert.AreEqual(startAbsoluteSeconds + interval * 4f, manager.PreciseAbsoluteGameSeconds, 0.001d,
                "Dispatching chronological tick context must not change or discard authoritative elapsed time.");
        }

        [Test]
        public void ShortTickAtMidnight_BelongsToDayWhoseIntervalItCompletes()
        {
            TimeManager manager = CreateTimeManager();
            float secondsPerDay = GetField<SimulationTimeSettings>(manager, "settings").GameSecondsPerDay;
            float interval = GetField<SimulationTimeSettings>(manager, "settings").ShortTickIntervalGameSeconds;
            SetField(manager, "gameSecondsIntoDay", secondsPerDay);
            SetField(manager, "shortTickAccumulatorGameSeconds", interval);
            var ticks = new List<SimulationTickContext>();
            manager.ShortTick += ticks.Add;

            InvokePrivate(manager, "ProcessShortTicks");

            Assert.AreEqual(1, ticks.Count);
            Assert.AreEqual(0, ticks[0].Date.AbsoluteDayIndex,
                "the final interval ending at midnight belongs to the day it completes");
            Assert.AreEqual(secondsPerDay, ticks[0].GameSecondsIntoDay, 0.001f);
            Assert.AreEqual(1f, ticks[0].TimeOfDay01, 0.000001f);
        }

        [Test]
        public void DeferredShortTickBurst_PreservesTheDateAcrossMidnight()
        {
            TimeManager manager = CreateTimeManager();
            float secondsPerDay = GetField<SimulationTimeSettings>(manager, "settings").GameSecondsPerDay;
            float interval = GetField<SimulationTimeSettings>(manager, "settings").ShortTickIntervalGameSeconds;
            SetField(manager, "absoluteDayIndex", 1);
            SetField(manager, "gameSecondsIntoDay", interval);
            SetField(manager, "shortTickAccumulatorGameSeconds", 2f * interval);
            var ticks = new List<SimulationTickContext>();
            manager.ShortTick += ticks.Add;

            InvokePrivate(manager, "ProcessShortTicks");

            Assert.AreEqual(2, ticks.Count);
            Assert.AreEqual(0, ticks[0].Date.AbsoluteDayIndex,
                "the deferred interval ending at midnight still belongs to the preceding day");
            Assert.AreEqual(secondsPerDay, ticks[0].GameSecondsIntoDay, 0.001f);
            Assert.AreEqual(1f, ticks[0].TimeOfDay01, 0.000001f);
            Assert.AreEqual(1, ticks[1].Date.AbsoluteDayIndex);
            Assert.AreEqual(interval, ticks[1].GameSecondsIntoDay, 0.001f);
            Assert.AreEqual(interval / secondsPerDay, ticks[1].TimeOfDay01, 0.000001f);
        }

        [Test]
        public void DayCollapse_ProcessesEveryCrossedDay()
        {
            // One rollover per frame forces the collapse path on the second boundary.
            TimeManager manager = CreateTimeManager(maxDayRolloversPerFrame: 1);
            SetField(manager, "gameSecondsIntoDay", 86300f);
            SetField(manager, "absoluteDayIndex", 6);

            int dayChanged = 0;
            int weekChanged = 0;
            var budgetDays = new List<int>();
            manager.DayChanged += ctx => { dayChanged++; };
            manager.WeekChanged += ctx => { weekChanged++; };
            // Simulate the daily driver contract: EnsureDay runs per DayChanged.
            var budgets = new WorkTimeBudgetStore();
            manager.DayChanged += ctx => budgetDays.Add(manager.CurrentAbsoluteDayIndex);

            InvokePrivate(manager, "AdvanceGameSeconds", 2f * 86400f + 100f);

            Assert.AreEqual(3, dayChanged,
                "crossing 3 day boundaries must fire DayChanged 3 times, not once");
            Assert.AreEqual(1, weekChanged,
                "the crossed week boundary (day 6 -> 7) must not be silently skipped");
            CollectionAssert.AreEqual(new[] { 7, 8, 9 }, budgetDays,
                "every crossed day's daily drivers must run (EnsureDay per day)");
            Assert.AreEqual(9, manager.CurrentAbsoluteDayIndex);
        }

        [Test]
        public void DayCollapse_SecondCallAfterCollapse_KeepsCalendarTruthful()
        {
            TimeManager manager = CreateTimeManager(maxDayRolloversPerFrame: 1);
            SetField(manager, "gameSecondsIntoDay", 86300f);
            SetField(manager, "absoluteDayIndex", 0);

            InvokePrivate(manager, "AdvanceGameSeconds", 2f * 86400f + 100f);
            float secondsIntoDay = manager.GameSecondsIntoDay;

            Assert.AreEqual(3, manager.CurrentAbsoluteDayIndex);
            Assert.GreaterOrEqual(secondsIntoDay, 0f);
            Assert.Less(secondsIntoDay, 86400f);
            Assert.AreEqual(2, manager.CollapsedDayRolloversLastFrame,
                "two of the three days went through the collapse path; the first rolled normally");
        }

        [Test]
        public void AbsoluteGameSeconds_MatchesDayPlusTimeOfDay()
        {
            TimeManager manager = CreateTimeManager();
            SetField(manager, "gameSecondsIntoDay", 3600f);
            SetField(manager, "absoluteDayIndex", 5);

            Assert.AreEqual(5f * 86400f + 3600f, manager.AbsoluteGameSeconds, 0.01f);
        }

        [Test]
        public void DayCollapse_PreservesWorkTicksBeforeEachDailyReset()
        {
            TimeManager manager = CreateTimeManager(maxShortTicksPerFrame: 64, maxDayRolloversPerFrame: 1);
            int ticks = 0;
            var ticksAtDailyReset = new List<int>();
            manager.ShortTick += _ => ticks++;
            manager.DayChanged += _ => ticksAtDailyReset.Add(ticks);

            InvokePrivate(manager, "AdvanceGameSeconds", 3f * 86400f + 900f);

            CollectionAssert.AreEqual(new[] { 96, 192, 288 }, ticksAtDailyReset,
                "Every closing day's work must run before its daily budget/reservation reset.");
            Assert.AreEqual(289, ticks, "The final partial day must retain its completed interval too.");
            Assert.AreEqual(3, manager.CurrentAbsoluteDayIndex);
            Assert.AreEqual(900f, manager.GameSecondsIntoDay, 0.01f);
        }

        [Test]
        public void FrameBudget_DefersTimeAndEventuallyProcessesEveryInterval()
        {
            TimeManager manager = CreateTimeManager(maxShortTicksPerFrame: 64, maxDayRolloversPerFrame: 1);
            int emitted = 0;
            var atReset = new List<int>();
            manager.ShortTick += _ => emitted++;
            manager.DayChanged += _ => atReset.Add(emitted);
            InvokePrivate(manager, "QueueAndAdvanceGameSeconds", 3f * 86400f + 900f);
            Assert.AreEqual(64, emitted);
            Assert.Greater(manager.PendingGameSeconds, 0d);
            TimeSaveDto saved = JsonUtility.FromJson<TimeSaveDto>(JsonUtility.ToJson(manager.CaptureSaveDto()));
            manager.LoadFromSaveDto(saved);
            Assert.AreEqual(saved.pendingGameSeconds, manager.PendingGameSeconds,
                "Save restoration preserves queued elapsed time.");
            for (int frame = 0; frame < 8 && manager.PendingGameSeconds > 0d; frame++)
                InvokePrivate(manager, "QueueAndAdvanceGameSeconds", 0f);
            Assert.AreEqual(0d, manager.PendingGameSeconds);
            Assert.AreEqual(289, emitted);
            CollectionAssert.AreEqual(new[] { 96, 192, 288 }, atReset);
            Assert.AreEqual(3, manager.CurrentAbsoluteDayIndex);
            Assert.AreEqual(900f, manager.GameSecondsIntoDay, 0.01f);
        }

        [Test]
        public void TinyRealTimeAdvances_ConserveSecondsLateInTheDayAcrossSaveRestore()
        {
            TimeManager manager = CreateTimeManager();
            SetField(manager, "gameSecondsIntoDay", 80000f);
            for (int frame = 0; frame < 1000; frame++)
            {
                InvokePrivate(manager, "QueueAndAdvanceGameSeconds", 0.01f);
                if (frame == 499)
                    manager.LoadFromSaveDto(JsonUtility.FromJson<TimeSaveDto>(JsonUtility.ToJson(manager.CaptureSaveDto())));
            }
            Assert.AreEqual(80010f, manager.GameSecondsIntoDay, 0.01f,
                "Low-speed frame fractions must not disappear into float rounding.");
        }
    }
}
