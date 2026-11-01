// Pass VIII (Worker 9): tick-size consistency and deterministic event
// ordering on the production clock (TimeManager). The same simulated
// duration advanced in different chunk sizes must produce the identical
// calendar, the identical ordered Day/Week/Month event sequence, and
// conserved short-tick accounting — no simulated seconds lost, no events
// skipped or reordered under acceleration.
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Time
{
    [TestFixture]
    public sealed class Pass8TickSizeConsistencyTests
    {
        private sealed class RunLog
        {
            public readonly List<string> Events = new List<string>();
            public int DayChanged;
            public int WeekChanged;
            public int MonthChanged;
        }

        private static TimeManager CreateClock(int maxDayRolloversPerFrame, int maxShortTicksPerFrame, RunLog log)
        {
            var manager = new TimeManager();
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            settings.shortTickIntervalGameMinutes = 15f;
            settings.maxShortTicksPerFrame = maxShortTicksPerFrame;
            settings.maxDayRolloversPerFrame = maxDayRolloversPerFrame;
            settings.Sanitize();
            SetField(manager, "settings", settings);
            SetField(manager, "absoluteDayIndex", 0);
            SetField(manager, "gameSecondsIntoDay", 0f);
            SetField(manager, "shortTickAccumulatorGameSeconds", 0f);
            SetField(manager, "shortTickIndex", 0);
            // Mirror TimeManager.InitializeState: currentDate must derive from
            // the anchor, otherwise the first rollover compares against a
            // default date and fires spurious Week/Month events.
            SetField(manager, "currentDate", settings.BuildDateFromAbsoluteDay(0));
            manager.DayChanged += _ =>
            {
                log.DayChanged++;
                log.Events.Add($"D{manager.CurrentAbsoluteDayIndex}");
            };
            manager.WeekChanged += _ =>
            {
                log.WeekChanged++;
                log.Events.Add($"W{manager.CurrentAbsoluteDayIndex}");
            };
            manager.MonthChanged += _ =>
            {
                log.MonthChanged++;
                log.Events.Add($"M{manager.CurrentAbsoluteDayIndex}");
            };
            return manager;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = typeof(TimeManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "TimeManager field '" + name + "' must exist");
            field.SetValue(target, value);
        }

        private static void Advance(TimeManager manager, float gameSeconds)
        {
            MethodInfo method = typeof(TimeManager).GetMethod("AdvanceGameSeconds", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "TimeManager.AdvanceGameSeconds must exist");
            method.Invoke(manager, new object[] { gameSeconds });
        }

        private static int ParseDay(string token)
        {
            return int.Parse(token.Substring(1));
        }

        [Test]
        public void FortyDays_OneChunkVsDailyChunks_IdenticalCalendarAndEventOrder()
        {
            const int days = 40;
            var smallLog = new RunLog();
            TimeManager small = CreateClock(maxDayRolloversPerFrame: 7, maxShortTicksPerFrame: 64, smallLog);
            for (int i = 0; i < days; i++)
            {
                Advance(small, 86400f);
            }

            var bigLog = new RunLog();
            TimeManager big = CreateClock(maxDayRolloversPerFrame: 1, maxShortTicksPerFrame: 64, bigLog);
            Advance(big, days * 86400f);

            Assert.AreEqual(small.CurrentAbsoluteDayIndex, big.CurrentAbsoluteDayIndex,
                "tick size must not change the resulting calendar day");
            Assert.AreEqual(smallLog.DayChanged, bigLog.DayChanged,
                "the same number of DayChanged events must fire regardless of chunk size");
            Assert.AreEqual(smallLog.WeekChanged, bigLog.WeekChanged,
                "week boundaries must not be skipped by the large chunk");
            Assert.AreEqual(smallLog.MonthChanged, bigLog.MonthChanged,
                "month boundaries must not be skipped by the large chunk");
            Assert.AreEqual(small.GameSecondsIntoDay, big.GameSecondsIntoDay, 0.01f,
                "time-of-day must converge");
            CollectionAssert.AreEqual(smallLog.Events, bigLog.Events,
                "the Day/Week/Month event sequence must be identical and ordered");
        }

        [Test]
        public void EventOrdering_WeekAndMonthAlwaysFollowTheirDayBoundary()
        {
            var log = new RunLog();
            TimeManager manager = CreateClock(maxDayRolloversPerFrame: 1, maxShortTicksPerFrame: 64, log);
            Advance(manager, 90f * 86400f);

            int lastDay = -1;
            for (int i = 0; i < log.Events.Count; i++)
            {
                string token = log.Events[i];
                int day = ParseDay(token);
                Assert.GreaterOrEqual(day, lastDay,
                    "calendar events must arrive in non-decreasing absolute-day order");
                lastDay = day;

                // A Week/Month event must immediately follow the Day event for
                // the same crossed boundary — never before it, never detached.
                if (token[0] == 'W' || token[0] == 'M')
                {
                    Assert.Greater(i, 0, "a Week/Month event cannot be first");
                    Assert.AreEqual($"D{day}", log.Events[i - 1],
                        "Week/Month events must directly follow their boundary's Day event");
                }
            }

            Assert.AreEqual(90, log.DayChanged, "90 crossed days must fire 90 DayChanged events");
            Assert.Greater(log.WeekChanged, 0, "week boundaries must be observed");
            Assert.Greater(log.MonthChanged, 0, "month boundaries must be observed");
        }

        [Test]
        public void ShortTicks_ConservedUnderAcceleration_NoSecondsLost()
        {
            const float interval = 900f; // 15-minute short ticks
            const int days = 10;
            const int expectedTicks = days * 96; // 86400 / 900 = 96 per day

            // Tight per-frame cap forces deferral; the accumulator must carry
            // every simulated second forward instead of discarding it. The
            // drain loop below stands in for later frames doing the same.
            var log = new RunLog();
            TimeManager manager = CreateClock(maxDayRolloversPerFrame: 7, maxShortTicksPerFrame: 8, log);
            int shortTicks = 0;
            manager.ShortTick += _ => shortTicks++;

            // Advance in several large calls so deferral happens repeatedly.
            for (int i = 0; i < 5; i++)
            {
                Advance(manager, 2f * 86400f);
                DrainShortTicks(manager);
            }

            DrainShortTicks(manager);
            Assert.AreEqual(expectedTicks, shortTicks,
                "every simulated 15-minute short tick must fire exactly once across the whole run");
            Assert.AreEqual(0f, GetAccumulator(manager), 0.01f,
                "no deferred seconds may remain unprocessed at the end of the run");
            Assert.AreEqual(days, manager.CurrentAbsoluteDayIndex);
        }

        private static void DrainShortTicks(TimeManager manager)
        {
            MethodInfo method = typeof(TimeManager).GetMethod("ProcessShortTicks", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < 1000 && GetAccumulator(manager) >= 900f; i++)
            {
                method.Invoke(manager, null);
            }
        }

        private static float GetAccumulator(TimeManager manager)
        {
            FieldInfo field = typeof(TimeManager).GetField("shortTickAccumulatorGameSeconds", BindingFlags.NonPublic | BindingFlags.Instance);
            return (float)field.GetValue(manager);
        }
    }
}
