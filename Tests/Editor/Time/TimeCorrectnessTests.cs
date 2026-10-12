using System.Collections.Generic;
using System.Reflection;
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
            SetField(manager, "shortTickAccumulatorGameSeconds", 100f * interval);
            int ticksBefore = manager.ShortTickIndex;

            InvokePrivate(manager, "ProcessShortTicks");

            Assert.AreEqual(ticksBefore + 64, manager.ShortTickIndex);
            Assert.AreEqual(36f * interval, GetField<float>(manager, "shortTickAccumulatorGameSeconds"), 0.001f,
                "overflow seconds must be deferred to later frames, never discarded");
            Assert.AreEqual(36, manager.CompressedShortTicksLastFrame);
        }

        [Test]
        public void ShortTickOverflow_DrainsAcrossFrames()
        {
            TimeManager manager = CreateTimeManager();
            float interval = 900f;

            SetField(manager, "shortTickAccumulatorGameSeconds", 100f * interval);
            InvokePrivate(manager, "ProcessShortTicks"); // 64 processed, 36 deferred
            InvokePrivate(manager, "ProcessShortTicks"); // 36 processed, 0 deferred

            Assert.AreEqual(100, manager.ShortTickIndex);
            Assert.AreEqual(0f, GetField<float>(manager, "shortTickAccumulatorGameSeconds"), 0.001f);
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
    }
}
