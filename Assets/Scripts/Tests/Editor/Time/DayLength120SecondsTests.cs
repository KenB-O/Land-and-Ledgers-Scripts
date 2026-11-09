using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Persistence;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Time
{
    /// <summary>
    /// The configured game day at 1x is five real minutes. Slice 0 discriminator first (must fail on the old
    /// baseline), then Slice 1 rate/rollover/pause/save coverage. Drives the
    /// clock through AdvanceForValidation / AdvanceGameSeconds via the
    /// reflection pattern already used in TimeCorrectnessTests.cs.
    /// </summary>
    [TestFixture]
    public sealed class DayLength300SecondsTests
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

        private static TimeManager CreateTimeManager()
        {
            var manager = new TimeManager();
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            settings.Sanitize();
            SetField(manager, "settings", settings);
            SetField(manager, "gameSecondsIntoDay", 0f);
            SetField(manager, "absoluteDayIndex", 0);
            SetField(manager, "shortTickAccumulatorGameSeconds", 0f);
            SetField(manager, "shortTickIndex", 0);
            return manager;
        }

        /// <summary>
        /// A manager at the initialized scenario start (day 4383, 21600s into
        /// the day, the default 0.25 start time). Advancing from here splits a
        /// full-day AdvanceGameSeconds call at the midnight boundary, so the
        /// per-frame 64-tick cap defers ticks into the second half of the call
        /// instead of leaving them deferred past the test's advance; a full
        /// day advanced from midnight in one call would leave 32 ticks
        /// deferred in the accumulator (deferral, not discard — the
        /// pre-existing, tested cap behaviour).
        /// </summary>
        private static TimeManager CreateInitializedTimeManager()
        {
            var manager = new TimeManager();
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            settings.Sanitize();
            SetField(manager, "settings", settings);
            manager.AdvanceForValidation(0f);
            return manager;
        }

        private static void DestroyManagerSettings(TimeManager manager)
        {
            SimulationTimeSettings settings = manager.Settings;
            if (settings != null)
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DefaultDayLength_Is300RealSecondsAt1x()
        {
            SimulationTimeSettings settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            try
            {
                Assert.AreEqual(300f, settings.realSecondsPerGameDayAt1x,
                    "a fresh SimulationTimeSettings must default to a 300-real-second day at 1x");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DayLength300Seconds_OneX_Advances288GameSecondsPerRealSecond()
        {
            TimeManager manager = CreateTimeManager();
            try
            {
                manager.SetSpeed(SimulationSpeed.Speed1x);
                Assert.AreEqual(288f, manager.CurrentGameSecondsPerRealSecond, 0.001f,
                    "at 300 real seconds per day, 1x advances 86400 / 300 = 288 game-seconds per real second");
            }
            finally
            {
                DestroyManagerSettings(manager);
            }
        }

        [Test]
        public void DayLength300Seconds_TenX_Advances2880GameSecondsPerRealSecond()
        {
            TimeManager manager = CreateTimeManager();
            try
            {
                manager.SetSpeed(SimulationSpeed.Speed10x);
                Assert.AreEqual(2880f, manager.CurrentGameSecondsPerRealSecond, 0.001f,
                    "at 10x a day takes 300 / 10 = 30 real seconds");
            }
            finally
            {
                DestroyManagerSettings(manager);
            }
        }

        [Test]
        public void DayLength300Seconds_TwentyFiveX_Advances7200GameSecondsPerRealSecond()
        {
            TimeManager manager = CreateTimeManager();
            try
            {
                manager.SetSpeed(SimulationSpeed.Speed25x);
                Assert.AreEqual(7200f, manager.CurrentGameSecondsPerRealSecond, 0.001f,
                    "at 25x a day takes 300 / 25 = 12 real seconds");
            }
            finally
            {
                DestroyManagerSettings(manager);
            }
        }

        [Test]
        public void DayLength300Seconds_MultiplierRelationshipPreserved()
        {
            var cases = new (SimulationSpeed speed, float multiplier)[]
            {
                (SimulationSpeed.Speed1x, 1f),
                (SimulationSpeed.Speed2x, 2f),
                (SimulationSpeed.Speed4x, 4f),
                (SimulationSpeed.Speed5x, 5f),
                (SimulationSpeed.Speed6x, 6f),
                (SimulationSpeed.Speed10x, 10f),
                (SimulationSpeed.Speed25x, 25f),
                (SimulationSpeed.Speed50x, 50f),
                (SimulationSpeed.Speed100x, 100f),
                (SimulationSpeed.Speed250x, 250f),
            };

            TimeManager manager = CreateTimeManager();
            try
            {
                foreach ((SimulationSpeed speed, float multiplier) in cases)
                {
                    manager.SetSpeed(speed);
                    float rate = manager.CurrentGameSecondsPerRealSecond;
                    Assert.AreEqual(multiplier, rate / 288f, 0.001f,
                        "rate at " + speed + " must be the configured multiplier times the 1x rate of 288");
                    Assert.AreEqual(300f / multiplier, 86400f / rate, 0.01f,
                        "a day at " + speed + " must take 300 / multiplier real seconds");
                }
            }
            finally
            {
                DestroyManagerSettings(manager);
            }
        }

        [Test]
        public void DayLength300Seconds_DayStillHas1440InGameMinutes()
        {
            TimeManager manager = CreateInitializedTimeManager();
            try
            {
                SimulationTimeSettings settings = manager.Settings;
                Assert.AreEqual(86400f, settings.GameSecondsPerDay, 0.001f);
                Assert.AreEqual(900f, settings.ShortTickIntervalGameSeconds, 0.001f);
                Assert.AreEqual(96, (int)(settings.GameSecondsPerDay / settings.ShortTickIntervalGameSeconds),
                    "a day still holds 96 fifteen-minute short ticks");

                int startDay = manager.CurrentAbsoluteDayIndex;
                int shortTicks = 0;
                int dayChanged = 0;
                manager.ShortTick += ctx => { shortTicks++; };
                manager.DayChanged += ctx => { dayChanged++; };

                manager.AdvanceForValidation(86400f);

                Assert.AreEqual(96, shortTicks, "advancing exactly one day must fire exactly 96 ShortTick events");
                Assert.AreEqual(1, dayChanged, "advancing exactly one day must fire exactly one DayChanged");
                Assert.AreEqual(startDay + 1, manager.CurrentAbsoluteDayIndex);
            }
            finally
            {
                DestroyManagerSettings(manager);
            }
        }

        [Test]
        public void DayLength300Seconds_DayRolloverFiresExactlyOncePerDay()
        {
            TimeManager manager = CreateTimeManager();
            try
            {
                int dayChanged = 0;
                var rolledDays = new List<int>();
                manager.DayChanged += ctx =>
                {
                    dayChanged++;
                    rolledDays.Add(manager.CurrentAbsoluteDayIndex);
                };

                InvokePrivate(manager, "AdvanceGameSeconds", 3f * 86400f + 100f);

                Assert.AreEqual(3, dayChanged,
                    "crossing 3 day boundaries must fire DayChanged 3 times, not once");
                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, rolledDays,
                    "each crossed day must roll over in order with consecutive day indices");
                Assert.AreEqual(3, manager.CurrentAbsoluteDayIndex);
                Assert.AreEqual(100f, manager.GameSecondsIntoDay, 0.001f);
            }
            finally
            {
                DestroyManagerSettings(manager);
            }
        }

        [Test]
        public void DayLength300Seconds_ShortTicksAndDueEventsStayOrdered()
        {
            TimeManager manager = CreateInitializedTimeManager();
            try
            {
                var scheduler = new DueEventScheduler();
                string ignored;
                float baseSeconds = manager.AbsoluteGameSeconds;
                Assert.IsNull(scheduler.Schedule(DueEventKind.TaskCompletionDue, baseSeconds + 3600f, "task-day0", out ignored));
                Assert.IsNull(scheduler.Schedule(DueEventKind.TendingDue, baseSeconds + 86400f + 3600f, "task-day1", out ignored));
                Assert.IsNull(scheduler.Schedule(DueEventKind.PassiveProcessComplete, baseSeconds + 2f * 86400f + 1800f, "proc-day2", out ignored));

                int shortTicks = 0;
                manager.ShortTick += ctx => { shortTicks++; };

                // Two full days plus 9000 game-seconds (10 short ticks): 202 ticks total.
                manager.AdvanceForValidation(2f * 86400f + 9000f);

                Assert.AreEqual(202, shortTicks,
                    "the longer day must lose no short tick and duplicate none: (2 * 86400 + 9000) / 900 = 202");

                List<DueEvent> due = scheduler.PopDue(manager.AbsoluteGameSeconds);
                Assert.AreEqual(3, due.Count);
                Assert.AreEqual("task-day0", due[0].PayloadRef);
                Assert.AreEqual("task-day1", due[1].PayloadRef);
                Assert.AreEqual("proc-day2", due[2].PayloadRef);
                Assert.Less(due[0].DueGameSeconds, due[1].DueGameSeconds);
                Assert.Less(due[1].DueGameSeconds, due[2].DueGameSeconds);
                Assert.AreEqual(0, scheduler.ScheduledCount, "each due event must pop exactly once");
                Assert.AreEqual(0, scheduler.PopDue(manager.AbsoluteGameSeconds).Count);
            }
            finally
            {
                DestroyManagerSettings(manager);
            }
        }

        [Test]
        public void DayLength300Seconds_PauseFreezesProgression()
        {
            TimeManager manager = CreateTimeManager();
            try
            {
                manager.SetSpeed(SimulationSpeed.Speed1x);
                manager.SetSpeed(SimulationSpeed.Paused);
                manager.SetPaused(true);

                Assert.IsTrue(manager.IsPaused);
                Assert.AreEqual(0f, manager.CurrentGameSecondsPerRealSecond, 0.001f,
                    "a paused clock advances zero game seconds per real second");

                manager.SetPaused(false);
                Assert.AreEqual(SimulationSpeed.Speed1x, manager.CurrentSpeed,
                    "unpausing must restore the previous non-paused speed");
                Assert.IsTrue(!manager.IsPaused);
                Assert.AreEqual(288f, manager.CurrentGameSecondsPerRealSecond, 0.001f);

                // Separately: pausing from RealTime resumes to RealTime.
                manager.SetSpeed(SimulationSpeed.RealTime);
                manager.SetPaused(true);
                Assert.IsTrue(manager.IsPaused);
                manager.SetPaused(false);
                Assert.AreEqual(SimulationSpeed.RealTime, manager.CurrentSpeed,
                    "unpausing from RealTime must restore RealTime, not 1x");
                Assert.AreEqual(1f, manager.CurrentGameSecondsPerRealSecond, 0.0001f);
            }
            finally
            {
                DestroyManagerSettings(manager);
            }
        }

        [Test]
        public void DayLength300Seconds_SaveLoadPreservesSimulationTime()
        {
            TimeManager manager = CreateTimeManager();
            try
            {
                Assert.IsTrue(manager.StartNewScenarioAtDate(1892, 3, 1));
                SetField(manager, "gameSecondsIntoDay", 0f);
                SetField(manager, "shortTickAccumulatorGameSeconds", 0f);
                SetField(manager, "shortTickIndex", 0);
                manager.SetSpeed(SimulationSpeed.Speed10x);

                manager.AdvanceForValidation(5f * 86400f + 3600f);
                Assert.AreEqual(5, manager.CurrentAbsoluteDayIndex);
                Assert.AreEqual(3600f, manager.GameSecondsIntoDay, 0.001f);

                // Mid-interval short-tick accumulator: the load must carry it exactly.
                int ticksBeforeSave = manager.ShortTickIndex;
                SetField(manager, "shortTickAccumulatorGameSeconds", 450f);

                TimeSaveDto save = manager.CaptureSaveDto();

                // A save carries no day-length field: the load path cannot depend on one.
                Assert.IsNull(typeof(TimeSaveDto).GetField("realSecondsPerGameDayAt1x"),
                    "TimeSaveDto must not store a day-length value");

                TimeManager loaded = CreateTimeManager();
                try
                {
                    loaded.LoadFromSaveDto(save);

                    Assert.AreEqual(1892, loaded.CurrentDate.Year);
                    Assert.AreEqual(3, loaded.CurrentDate.Month);
                    Assert.AreEqual(6, loaded.CurrentDate.DayOfMonth);
                    Assert.AreEqual(5, loaded.CurrentAbsoluteDayIndex);
                    Assert.AreEqual(3600f, loaded.GameSecondsIntoDay, 0.001f);
                    Assert.AreEqual(3600f / 86400f, loaded.TimeOfDay01, 0.0001f);
                    Assert.AreEqual(ticksBeforeSave, loaded.ShortTickIndex);
                    Assert.AreEqual(450f, GetField<float>(loaded, "shortTickAccumulatorGameSeconds"), 0.001f);
                    Assert.AreEqual(SimulationSpeed.Speed10x, loaded.CurrentSpeed);

                    // Day length comes from settings, not the save: at 1x the loaded
                    // manager still runs a 300-second day.
                    loaded.SetSpeed(SimulationSpeed.Speed1x);
                    Assert.AreEqual(288f, loaded.CurrentGameSecondsPerRealSecond, 0.001f);
                }
                finally
                {
                    DestroyManagerSettings(loaded);
                }
            }
            finally
            {
                DestroyManagerSettings(manager);
            }
        }
    }
}
