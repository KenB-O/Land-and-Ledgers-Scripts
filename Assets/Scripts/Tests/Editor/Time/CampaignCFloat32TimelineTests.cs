// Campaign C, package 11 — DEF-046 (float32 absolute timeline),
// Nov 15, 2026.
//
// VERDICT (verify-then-document): CONFIRMED as A-to-Z Check 9 F-02
// states, and quantified precisely. The absolute timeline every
// scheduler consumes is float32, derived as a cast of a double
// timeline that already exists. At 25 simulated years the float32
// quantum is 64 s, which exceeds the 60 s smallest scheduling unit
// (the 1-minute work floor), so two events intended 60 s apart can
// collapse to the same representable instant and pop in scheduling
// order instead of time order — an inversion of intended order.
// The first collapse horizon is earlier than Check 9's "at 25 years"
// wording suggests: the quantum first reaches 64 s when absolute
// game seconds reach 2^29, i.e. during day 6214 (~17.01 years in),
// not at 25 years. Below that magnitude the quantum is at most 32 s,
// so a 60 s-apart pair can never collapse (rounded values differ by
// at least 60 - quantum > 0).
//
// What is NOT fixed here (deliberate): a double-precision timeline
// is a major architectural change — it touches every scheduler, the
// save DTOs, the TaskAuthority timing clock, the domestic tick gate,
// and every Func<float> NowGameSeconds supplier. This package pins
// the current behaviour with tests and documents the exact state,
// the quantum table, the affected schedulers, the partial workarounds
// that already exist, and what a migration would have to touch. No
// production code is changed.
//
// Three test kinds, same file:
//  (1) Real unit tests executing genuine float32 arithmetic — the
//      quantum table, the collapse pair, the scheduler inversion on
//      the real DueEventScheduler, and the domestic day/minute
//      derivation exactly as DomesticLaborDriver.AdvanceTick computes
//      it (DomesticLaborDriver.cs:230-231).
//  (2) Reflection tests on the real contract types — DueEvent's due
//      time is float, the scheduler interface is float, and the save
//      DTO persists those float due times.
//  (3) Structural tests pinning the wiring by reading the production
//      files — the double timeline exists but has exactly two
//      production consumers, the clock's own day-rollover math is
//      already double, saves are JsonUtility JSON, and the drivers
//      carry a second float absolute-time source.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...). A missing repo fails loudly, never
// silently passes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Time
{
    [TestFixture]
    public sealed class CampaignCFloat32TimelineTests
    {
        private const string SaveLoadManagerPath =
            "Assets/Scripts/Infrastructure/Persistence/SaveLoadManager.cs";
        private const string TimeManagerPath =
            "Assets/Scripts/Core/Time/TimeManager.cs";
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";

        // The smallest scheduling unit in the sim: the 1-minute work
        // floor (TaskAuthority.EstimateMinutesForBase clamps to min 1;
        // the shopping leg poll and the passive-resume retry are 60 s).
        private const double SmallestSchedulingUnitSeconds = 60.0;

        // 2^29 absolute game seconds: the first magnitude whose float32
        // quantum is 64 s. Day 6214 of the campaign (~17.01 years in at
        // 86400 game-seconds per day).
        private const double FirstQuantum64Seconds = 536870912.0;

        // ---------- (1) Quantum table, real float32 arithmetic ----------

        [Test]
        public void QuantumAtDay250_Is2Seconds()
        {
            // The dev scenario (~day 250) lives at a 2 s quantum: every
            // 1-minute due time is still exactly representable.
            Assert.AreEqual(2f, Quantum((float)(250.0 * 86400.0)));
        }

        [Test]
        public void QuantumAtDay1000_Is8Seconds()
        {
            Assert.AreEqual(8f, Quantum((float)(1000.0 * 86400.0)));
        }

        [Test]
        public void QuantumTable_OneFiveTenTwentyFiveYears()
        {
            Assert.AreEqual(2f, Quantum((float)(365.0 * 86400.0)),
                "1 year: 31.5M s, quantum 2 s.");
            Assert.AreEqual(16f, Quantum((float)(5.0 * 365.25 * 86400.0)),
                "5 years: 157.8M s, quantum 16 s.");
            Assert.AreEqual(32f, Quantum((float)(10.0 * 365.25 * 86400.0)),
                "10 years: 315.6M s, quantum 32 s.");
            Assert.AreEqual(64f, Quantum((float)(25.0 * 365.25 * 86400.0)),
                "25 years: 788.9M s, quantum 64 s — exceeds the 60 s unit.");
        }

        [Test]
        public void FirstQuantum64_IsExactlyTwoToThe29th()
        {
            // The quantum jumps 32 -> 64 exactly at 2^29. In campaign
            // terms that is 536,870,912 / 86,400 = day 6213.78, i.e.
            // during day index 6213 (the 6,214th day, ~17.01 years in).
            // Check 9's "at 25 years" is true but late: the 60 s unit
            // first collapses ~8 years earlier.
            Assert.AreEqual(32f, Quantum((float)Math.Pow(2.0, 28.0)));
            Assert.AreEqual(64f, Quantum((float)Math.Pow(2.0, 29.0)));
            Assert.AreEqual(6213.0, Math.Floor(FirstQuantum64Seconds / 86400.0),
                "2^29 s falls on day index 6213.");
            Assert.IsTrue(FirstQuantum64Seconds / 86400.0 / 365.25 > 17.0
                && FirstQuantum64Seconds / 86400.0 / 365.25 < 17.1,
                "2^29 s is ~17.01 years in.");
        }

        [Test]
        public void SixtySecondPair_CollapsesAtTwentyFiveYears()
        {
            // A concrete collapsing pair: t = 2^29 + 33 s. The two
            // double due times are exactly 60 s apart, but both cast
            // to the same float (536,870,976) — the earlier one rounds
            // up 31 s, the later one rounds down 29 s.
            double earlier = FirstQuantum64Seconds + 33.0;
            double later = earlier + SmallestSchedulingUnitSeconds;
            Assert.AreEqual(60.0, later - earlier,
                "The intended due times really are 60 s apart.");
            float earlierFloat = (float)earlier;
            float laterFloat = (float)later;
            Assert.AreEqual(536870976f, earlierFloat);
            Assert.AreEqual(earlierFloat, laterFloat,
                "At the 64 s quantum the 60 s-apart pair collapses to "
                + "one representable instant.");
        }

        [Test]
        public void SixtySecondPair_NeverCollapsesAtTenYears()
        {
            // At 10 years the quantum is 32 s < 60 s, so two values
            // 60 s apart round to floats at least 60 - 32 = 28 s apart.
            // Spot-check three alignments; the analytic bound
            // (quantum < 60) is what rules collapse out entirely.
            Assert.IsTrue(Quantum((float)Math.Pow(2.0, 28.0)) < 60f,
                "Quantum 32 s is below the 60 s unit, so a 60 s-apart "
                + "pair cannot round equal.");
            foreach (int offset in new[] { 0, 16, 31 })
            {
                double t = Math.Pow(2.0, 28.0) + offset;
                Assert.AreNotEqual((float)t, (float)(t + 60.0),
                    "10y offset " + offset + ": the pair stays distinct.");
            }
        }

        [Test]
        public void RoundingError_AtTwentyFiveYears_BoundedBy32Seconds()
        {
            // A due time cast to float at 25 years is wrong by at most
            // half the quantum (32 s) — and the collapsing pair above
            // shows the bound is nearly attained (31 s early).
            double t = FirstQuantum64Seconds + 33.0;
            Assert.AreEqual(31.0, (float)t - t, 0.001,
                "The earlier due time rounds 31 s late.");
            Assert.AreEqual(32f, Quantum((float)(25.0 * 365.25 * 86400.0)) / 2f,
                "Half-quantum bound at 25 years is 32 s.");
        }

        [Test]
        public void DueEventScheduler_InvertsSixtySecondPairAtTwentyFiveYears()
        {
            // The real scheduler orders by (dueGameSeconds, sequence).
            // With collapsed due floats the sequence — scheduling order,
            // not time order — decides. The later-intended event was
            // scheduled first, so it pops first: an inversion of the
            // intended time order, on the real heap implementation.
            double earlier = FirstQuantum64Seconds + 33.0;
            double later = earlier + SmallestSchedulingUnitSeconds;
            var scheduler = new DueEventScheduler();
            string problem = scheduler.Schedule(DueEventKind.TaskCompletionDue,
                (float)later, "task-later", out _);
            Assert.IsNull(problem);
            problem = scheduler.Schedule(DueEventKind.TaskCompletionDue,
                (float)earlier, "task-earlier", out _);
            Assert.IsNull(problem);
            List<DueEvent> popped = scheduler.PopDue((float)later);
            Assert.AreEqual(2, popped.Count);
            Assert.AreEqual("task-later", popped[0].PayloadRef,
                "The later-intended event pops first: intended order "
                + "is inverted once the due floats collapse.");
            Assert.AreEqual("task-earlier", popped[1].PayloadRef);
        }

        [Test]
        public void DueEventScheduler_PreservesSixtySecondOrderAtTenYears()
        {
            // Same setup at 10 years, where the pair stays distinct:
            // the earlier-intended event pops first, as intended.
            double earlier = Math.Pow(2.0, 28.0) + 16.0;
            double later = earlier + SmallestSchedulingUnitSeconds;
            var scheduler = new DueEventScheduler();
            Assert.IsNull(scheduler.Schedule(DueEventKind.TaskCompletionDue,
                (float)later, "task-later", out _));
            Assert.IsNull(scheduler.Schedule(DueEventKind.TaskCompletionDue,
                (float)earlier, "task-earlier", out _));
            List<DueEvent> popped = scheduler.PopDue((float)later);
            Assert.AreEqual(2, popped.Count);
            Assert.AreEqual("task-earlier", popped[0].PayloadRef);
            Assert.AreEqual("task-later", popped[1].PayloadRef);
        }

        [Test]
        public void DomesticMinuteDerivation_SkipsMinutesAtTwentyFiveYears()
        {
            // DomesticLaborDriver.AdvanceTick derives the minute of day
            // from the float absolute time (DomesticLaborDriver.cs:230-231):
            //   dayIndex = floor(currentGameSeconds / 86400f)
            //   minuteOfDay = floor((currentGameSeconds
            //       - dayIndex * 86400f) / 60f)
            // At the 64 s quantum the intraday remainder steps in 64 s
            // increments, so the derived minute skips 15, 31, 47 and 59
            // in the first hour of day 9131 (25 years in). The start
            // gate is ">=" so a task planned for minute 15 starts at
            // minute 16 instead — up to ~64 s late, never missed — but
            // a minute-exact window has no exact minute to hit.
            double baseSeconds = 9131.0 * 86400.0;
            var derived = new HashSet<int>();
            for (int minute = 0; minute < 60; minute++)
            {
                float f = (float)(baseSeconds + minute * 60.0);
                int dayIndex = (int)Math.Floor(f / 86400f);
                int minuteOfDay = (int)Math.Floor((f - dayIndex * 86400f) / 60f);
                Assert.AreEqual(9131, dayIndex,
                    "True minute " + minute + " must still derive day 9131.");
                derived.Add(minuteOfDay);
            }
            foreach (int skipped in new[] { 15, 31, 47, 59 })
            {
                Assert.IsFalse(derived.Contains(skipped),
                    "Derived minutes skip " + skipped + " at 25 years.");
            }
            float atFifteen = (float)(baseSeconds + 15.0 * 60.0);
            int atFifteenDay = (int)Math.Floor(atFifteen / 86400f);
            int atFifteenMinute = (int)Math.Floor(
                (atFifteen - atFifteenDay * 86400f) / 60f);
            Assert.AreEqual(14, atFifteenMinute,
                "True 00:15 derives minute 14 at 25 years.");
        }

        [Test]
        public void DomesticDayDerivation_ReadsTomorrowWithin32sBeforeMidnight()
        {
            // 10 s before midnight on day 9131 the float rounds UP to
            // midnight itself, so the derived day index already reads
            // tomorrow (9131) while the true day is still 9130. A short
            // tick landing in that 32 s window would prune today's
            // unstarted domestic plans as stale (PruneStalePending
            // drops DayIndex < derived) and skip today's starts
            // (DayIndex != derived) for that tick.
            double trueSeconds = 9131.0 * 86400.0 - 10.0;
            float f = (float)trueSeconds;
            int trueDay = (int)Math.Floor(trueSeconds / 86400.0);
            int derivedDay = (int)Math.Floor(f / 86400f);
            Assert.AreEqual(9130, trueDay);
            Assert.AreEqual(9131, derivedDay,
                "Within 32 s before midnight the float-derived day "
                + "already reads tomorrow.");
        }

        [Test]
        public void DomesticDayDerivation_CorrectWithin32sAfterMidnight()
        {
            // The asymmetry: 10 s AFTER midnight the float rounds DOWN
            // to midnight itself, which is the correct day. The
            // day-index error only ever points forward, never back.
            double trueSeconds = 9131.0 * 86400.0 + 10.0;
            float f = (float)trueSeconds;
            int trueDay = (int)Math.Floor(trueSeconds / 86400.0);
            int derivedDay = (int)Math.Floor(f / 86400f);
            Assert.AreEqual(9131, trueDay);
            Assert.AreEqual(9131, derivedDay,
                "After midnight the float-derived day is correct.");
        }

        [Test]
        public void IntradayFloat_StaysPrecise()
        {
            // The intraday part needs no migration: gameSecondsIntoDay
            // tops out at 86,400 s, where the float32 quantum is
            // 0.0078125 s — far below any scheduling unit. Only the
            // ABSOLUTE float (day index * 86,400 + intraday) is
            // affected, because its magnitude is what sets the quantum.
            Assert.AreEqual(0.0078125f, Quantum(86400f), 0.0000001f,
                "Intraday quantum at 86,400 s is ~8 ms.");
        }

        // ---------- (2) Reflection on the real contract types ----------

        [Test]
        public void DueEvent_DueGameSeconds_IsFloat()
        {
            FieldInfo field = typeof(DueEvent).GetField("dueGameSeconds",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "DueEvent must still carry dueGameSeconds.");
            Assert.AreEqual(typeof(float), field.FieldType,
                "DueEvent.dueGameSeconds is float — the persisted due "
                + "time is a float32 absolute time.");
        }

        [Test]
        public void SchedulerInterface_IsFloat()
        {
            MethodInfo schedule = typeof(IDueEventScheduler).GetMethod("Schedule");
            Assert.IsNotNull(schedule);
            Assert.AreEqual(typeof(float), schedule.GetParameters()[1].ParameterType,
                "IDueEventScheduler.Schedule takes a float due time.");
            MethodInfo popDue = typeof(IDueEventScheduler).GetMethod("PopDue");
            Assert.IsNotNull(popDue);
            Assert.AreEqual(typeof(float), popDue.GetParameters()[0].ParameterType,
                "IDueEventScheduler.PopDue takes a float current time.");
            // TryGetDueGameSeconds lives on the concrete scheduler, not
            // the interface (it is the task-duration diagnostic's L5
            // evidence feed).
            MethodInfo tryGet = typeof(DueEventScheduler).GetMethod("TryGetDueGameSeconds");
            Assert.IsNotNull(tryGet);
            Type dueOut = tryGet.GetParameters()[1].ParameterType;
            Assert.IsTrue(dueOut.IsByRef && dueOut.GetElementType() == typeof(float),
                "TryGetDueGameSeconds reports a float due time.");
        }

        [Test]
        public void SchedulerSaveDto_PersistsFloatDueTimes()
        {
            // The float due times are what get saved: the DTO holds the
            // live DueEvent list, so a double migration must change the
            // field type inside DueEvent itself (see the checkpoint's
            // save-compatibility note — saves are JsonUtility JSON, so
            // old saves still parse).
            FieldInfo events = typeof(DueEventSchedulerSaveDto).GetField("events");
            Assert.IsNotNull(events);
            Assert.AreEqual(typeof(List<DueEvent>), events.FieldType);
        }

        // ---------- (3) Structural pins on the production wiring ----------

        [Test]
        public void DoubleTimeline_ExistsButHasTwoProductionConsumers()
        {
            // The double timeline exists (TimeManager.cs:144-149) but
            // only two production consumers use it. Everything that
            // schedules work uses the float cast.
            string timeManager = ReadRepoFile(TimeManagerPath);
            Assert.IsTrue(timeManager.Contains("public double PreciseAbsoluteGameSeconds"),
                "The double timeline must still exist.");
            Assert.IsTrue(timeManager.Contains(
                "public float AbsoluteGameSeconds => (float)PreciseAbsoluteGameSeconds;"),
                "The float timeline must still be a cast of the double one.");
            var users = new List<string>();
            string scriptsRoot = Path.Combine(FindRepoRoot(), "Assets", "Scripts");
            foreach (string file in Directory.GetFiles(scriptsRoot, "*.cs",
                SearchOption.AllDirectories))
            {
                if (file.Contains("Tests" + Path.DirectorySeparatorChar))
                    continue;
                if (File.ReadAllText(file).Contains("PreciseAbsoluteGameSeconds"))
                    users.Add(Path.GetFileName(file));
            }
            Assert.AreEqual(3, users.Count,
                "Exactly three production files may reference the double "
                + "timeline (its definition + two consumers); got: "
                + string.Join(", ", users));
            Assert.IsTrue(users.Contains("TimeManager.cs"));
            Assert.IsTrue(users.Contains("AgentMover.cs"),
                "AgentMover (route advancement) is a double consumer.");
            Assert.IsTrue(users.Contains("WorldInteractionController.cs"),
                "WorldInteractionController (observation timing) is a "
                + "double consumer.");
        }

        [Test]
        public void DayRolloverMath_IsDouble()
        {
            // The clock's own day arithmetic is already double
            // (CollapseRemainingDayRollovers works in double), so the
            // day boundary itself does not drift — only the derived
            // float absolute time and its consumers are affected.
            string timeManager = ReadRepoFile(TimeManagerPath);
            Assert.IsTrue(timeManager.Contains("double totalSecondsToPlace"),
                "Day-rollover placement must stay in double.");
        }

        [Test]
        public void SavePipeline_IsJson()
        {
            // Saves are JsonUtility JSON (SaveLoadManager.cs:276, :755),
            // which is what makes a future float->double field change
            // load-compatible for old saves: a JSON number parses into
            // a double field. No binary serialization of due events
            // exists in Core/Time or Infrastructure/Persistence.
            string saveLoad = ReadRepoFile(SaveLoadManagerPath);
            Assert.IsTrue(saveLoad.Contains("JsonUtility.ToJson"),
                "Saves must still serialize through JsonUtility.");
            Assert.IsTrue(saveLoad.Contains("JsonUtility.FromJson"),
                "Loads must still deserialize through JsonUtility.");
        }

        [Test]
        public void SecondFloatAbsoluteSource_ExistsInDrivers()
        {
            // SimulationDrivers carries a SECOND float absolute-time
            // computation beside TimeManager.AbsoluteGameSeconds:
            // AbsoluteNowGameSeconds() recomputes it as int * float +
            // float. The two can disagree by up to a quantum, and a
            // double migration must convert both, not just the
            // TimeManager property.
            string drivers = ReadRepoFile(DriversPath);
            Assert.IsTrue(drivers.Contains("private float AbsoluteNowGameSeconds()"),
                "The drivers' own float absolute-time helper must "
                + "still exist.");
            Assert.IsTrue(drivers.Contains(
                "CurrentAbsoluteDayIndex * 86400f + timeManager.GameSecondsIntoDay"),
                "The drivers' helper must still recompute the absolute "
                + "time in float.");
        }

        // ---------- Helpers ----------

        /// <summary>
        /// Float32 spacing at a value: the distance to the next
        /// representable float above it.
        /// </summary>
        private static float Quantum(float value)
        {
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            float next = BitConverter.ToSingle(BitConverter.GetBytes(bits + 1), 0);
            return next - value;
        }

        // ---------- Repo helpers (C-5 / C-7 / C-8 / C-9 precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, SaveLoadManagerPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
            };
            var tried = new StringBuilder();
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (string candidate in new[]
                    {
                        dir,
                        Path.Combine(dir, "work-c"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, SaveLoadManagerPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (package 11): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (package 11): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }
    }
}
