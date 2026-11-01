using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// Pass IX WS6 (A1-01/A1-02/A1-03): scenario bootstrap from a captured
    /// selection, the canonical date anchor, and scenario failure states.
    /// The preset's start date must drive the session clock through the
    /// public TimeManager.LoadFromSaveDto seam; a missed dated deadline
    /// must fail the scenario exactly once and persist.
    /// </summary>
    [TestFixture]
    public sealed class Pass9ScenarioBootstrapTests
    {
        private readonly List<GameObject> timeManagerObjects = new List<GameObject>();
        private readonly List<SimulationTimeSettings> timeSettings = new List<SimulationTimeSettings>();

        [SetUp]
        public void SetUp()
        {
            timeManagerObjects.Clear();
            timeSettings.Clear();
            ScenarioRunStateEvents.ClearSubscribers();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = timeManagerObjects.Count - 1; i >= 0; i--)
            {
                if (timeManagerObjects[i] != null)
                    Object.DestroyImmediate(timeManagerObjects[i]);
            }

            for (int i = timeSettings.Count - 1; i >= 0; i--)
            {
                if (timeSettings[i] != null)
                    Object.DestroyImmediate(timeSettings[i]);
            }

            ScenarioRunStateEvents.ClearSubscribers();
        }

        // ---------- A1-02: date authority ----------

        [Test]
        public void DateAuthority_AnchorFrame_Jan1OfAnchorYearIsZero()
        {
            Assert.AreEqual(1880, ScenarioDateUtil.AnchorYear);
            Assert.AreEqual(CalendarAnchor.DefaultAnchorYear, ScenarioDateUtil.AnchorYear,
                "the util's anchor year derives from CalendarAnchor — one authority");
            Assert.AreEqual(0, ScenarioDateUtil.DayIndexOfJan1(ScenarioDateUtil.AnchorYear));
            Assert.AreEqual(3652, ScenarioDateUtil.DaysBeforeAnchorYear,
                "1870-01-01 -> 1880-01-01 is 10 years incl. the 1872 and 1876 leap days");
            Assert.AreEqual(ScenarioDateUtil.ToDayIndex(1880, 1, 1), ScenarioDateUtil.DaysBeforeAnchorYear);
        }

        [Test]
        public void DateAuthority_AnchorDayIndex_MatchesDateUtilFrame()
        {
            Assert.AreEqual(60, CalendarAnchor.Default.AnchorDayIndex, "1880-03-01 is day 60 of the 1880 frame (leap year)");
            Assert.AreEqual(CalendarAnchor.Default.AnchorDayIndex, ScenarioDateUtil.DayIndexForDate(1880, 3, 1));
            Assert.AreEqual(60, ScenarioDateUtil.DayIndexForFirstOfMonth(1880, 3));
            Assert.AreEqual(0, ScenarioDateUtil.DayIndexForFirstOfMonth(1880, 1));
            // Frame conversion: absolute day (TimeManager) + AnchorDayIndex == anchor-frame day.
            Assert.AreEqual(ScenarioDateUtil.DayIndexForDate(1881, 3, 1),
                CalendarAnchor.Default.ToAbsoluteDay(1881, 3, 1) + CalendarAnchor.Default.AnchorDayIndex);
        }

        // ---------- A1-01: captured selection ----------

        [Test]
        public void CapturedSelection_RoundTrips_AndLegacyTitleOnlyJsonIsAccepted()
        {
            var selection = new CapturedScenarioSelection("slice-l-hearth-and-ledger", "Slice L", "1.0.0");
            CapturedScenarioSelection parsed = CapturedScenarioSelection.FromJson(selection.ToJson());
            Assert.AreEqual("slice-l-hearth-and-ledger", parsed.Id);
            Assert.AreEqual("Slice L", parsed.Title);
            Assert.AreEqual("1.0.0", parsed.Version);

            CapturedScenarioSelection legacy = CapturedScenarioSelection.FromJson("{\"title\": \"Slice L\"}");
            Assert.AreEqual("", legacy.Id);
            Assert.AreEqual("Slice L", legacy.Title);

            CapturedScenarioSelection bare = CapturedScenarioSelection.FromJson("Slice L");
            Assert.AreEqual("Slice L", bare.Title);

            Assert.AreEqual("", CapturedScenarioSelection.FromJson(null).Title, "null input yields an empty selection, never throws");
            Assert.AreEqual("", CapturedScenarioSelection.FromJson("not json at all {").Id);
        }

        // ---------- A1-01: bootstrap plan + clock ----------

        private TimeManager CreateTimeManager()
        {
            var timeManagerObject = new GameObject("Pass9ScenarioBootstrapTests_TimeManager");
            timeManagerObjects.Add(timeManagerObject);
            timeManagerObject.SetActive(false);

            TimeManager manager = timeManagerObject.AddComponent<TimeManager>();
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            timeSettings.Add(settings);
            settings.Sanitize();
            FieldInfo field = typeof(TimeManager).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(manager, settings);

            // Activate only after the serialized settings are installed so Awake
            // initializes the same component/lifecycle used by the runtime clock.
            timeManagerObject.SetActive(true);
            return manager;
        }

        [Test]
        public void Bootstrap_PresetStartDate_DrivesTheClock()
        {
            ScenarioPreset preset = SliceLPresetFactory.Build(1881, 3, 1);
            var selection = new CapturedScenarioSelection(preset.PresetId, preset.PresetName);
            var diag = new List<string>();

            Assert.IsTrue(ScenarioBootstrapFactory.TryCreateStart(selection, new[] { preset }, out ScenarioBootstrapPlan plan, diag),
                string.Join(";", diag));
            Assert.IsTrue(plan.Composition.IsPreset);
            Assert.IsTrue(plan.UsePresetStartDate);
            Assert.AreEqual("first-ledger-slice-l", plan.ScenarioId);
            Assert.AreEqual(1881, plan.StartYear);
            Assert.AreEqual(365, plan.StartDayIndex, "1881-03-01 is 365 days after the 1880-03-01 anchor");
            Assert.AreEqual(ScenarioDateUtil.DayIndexFor(1881, 3, 1), plan.AnchorFrameDayIndex);

            TimeManager clock = CreateTimeManager();
            Assert.IsTrue(ScenarioBootstrapFactory.ApplyToTimeManager(plan, clock));
            Assert.AreEqual(365, clock.CurrentAbsoluteDayIndex);
            Assert.AreEqual(1881, clock.CurrentDate.Year);
            Assert.AreEqual(3, clock.CurrentDate.Month);
            Assert.AreEqual(1, clock.CurrentDate.DayOfMonth);
        }

        [Test]
        public void Bootstrap_DefaultSliceL_StartsAtTheAnchorDate()
        {
            ScenarioPreset preset = SliceLPresetFactory.Build();
            Assert.IsTrue(ScenarioBootstrapFactory.TryCreateStart(preset, out ScenarioBootstrapPlan plan, new List<string>()));
            Assert.AreEqual(1880, plan.StartYear);
            Assert.AreEqual(0, plan.StartDayIndex, "the default Slice L start IS the anchor date");
            Assert.AreEqual(60, plan.AnchorFrameDayIndex);

            TimeManager clock = CreateTimeManager();
            Assert.IsTrue(ScenarioBootstrapFactory.ApplyToTimeManager(plan, clock));
            Assert.AreEqual(0, clock.CurrentAbsoluteDayIndex);
        }

        [Test]
        public void Bootstrap_UnknownSelection_FallsBackToAnchorDate_NotPreset()
        {
            var selection = new CapturedScenarioSelection("", "Some Old Save Title");
            Assert.IsTrue(ScenarioBootstrapFactory.TryCreateStart(selection, new ScenarioPreset[0],
                out ScenarioBootstrapPlan plan, new List<string>()));
            Assert.IsFalse(plan.Composition.IsPreset);
            Assert.IsFalse(plan.UsePresetStartDate);
            Assert.AreEqual("legacy", plan.Composition.Version);
            Assert.AreEqual(0, plan.StartDayIndex);
            Assert.AreEqual(1880, plan.StartYear);

            Assert.IsFalse(ScenarioBootstrapFactory.TryCreateStart(null, null, out _, new List<string>()),
                "no selection, no start");
            Assert.IsFalse(ScenarioBootstrapFactory.ApplyToTimeManager(null, CreateTimeManager()));
            Assert.IsFalse(ScenarioBootstrapFactory.ApplyToTimeManager(plan, null));
        }

        [Test]
        public void Bootstrap_LegacyTitleOnlySelection_ResolvesPresetByTitle()
        {
            ScenarioPreset preset = SliceLPresetFactory.Build();
            CapturedScenarioSelection legacy = CapturedScenarioSelection.FromJson(
                "{\"title\": \"" + preset.PresetName + "\"}");
            ScenarioBootstrapComposition composition =
                ScenarioBootstrapFactory.ResolveComposition(legacy, new[] { preset }, out ScenarioPreset matched);
            Assert.IsNotNull(matched);
            Assert.IsTrue(composition.IsPreset);
            Assert.AreEqual(preset.PresetVersion, composition.Version);
        }

        // ---------- A1-03: failure states + headline snapshot ----------

        // Mirrors SaloonCircuitScenario.DeadlineDayByGoal (the Saloon files
        // are not compiled by the harness): ash-creek-ownership = day 365.
        private const string SaloonGoalId = "ash-creek-ownership";
        private const int SaloonDeadlineDay = 365;

        [Test]
        public void Failure_MissedSaloonDeadline_FailsExactlyOnce_AndPersists()
        {
            int eventCount = 0;
            string eventScenario = null, eventGoal = null;
            ScenarioRunStateEvents.ScenarioFailed += (scenarioId, goalId) =>
            {
                eventCount++;
                eventScenario = scenarioId;
                eventGoal = goalId;
            };

            var service = new ScenarioObjectiveService();
            service.BeginScenario(ScenarioObjectiveService.FirstLedgerScenarioId);
            bool ownsAshCreek = false;
            service.RegisterDatedQualifier(SaloonGoalId, SaloonDeadlineDay, () => ownsAshCreek);

            Assert.IsFalse(service.Evaluate(SaloonDeadlineDay), "on the deadline day the qualifier still has its day");
            Assert.IsFalse(service.IsFailed);

            Assert.IsTrue(service.Evaluate(SaloonDeadlineDay + 1), "past the deadline, unqualified: the run fails");
            Assert.IsTrue(service.IsFailed);
            Assert.AreEqual(1, eventCount);
            Assert.AreEqual("the-first-ledger", eventScenario);
            Assert.AreEqual(SaloonGoalId, eventGoal);
            Assert.AreEqual(1, service.State.FailedScenarioIds.Count);
            Assert.AreEqual("the-first-ledger", service.State.FailedScenarioIds[0]);

            // Sticky + exactly once: later evaluations never re-emit, and a
            // late qualification does not un-fail the run.
            ownsAshCreek = true;
            Assert.IsFalse(service.Evaluate(SaloonDeadlineDay + 30));
            Assert.AreEqual(1, eventCount);
            Assert.IsTrue(service.IsFailed);

            // Persisted run state round-trips (hub Capture/Load shape).
            ScenarioRunState saved = service.CaptureRunState();
            var restored = new ScenarioObjectiveService();
            restored.LoadRunState(saved);
            Assert.IsTrue(restored.IsFailed);
            Assert.AreEqual(1, restored.State.FailedGoalIds.Count);
            Assert.AreEqual(SaloonGoalId, restored.State.FailedGoalIds[0]);

            // Headline snapshot carries the failure state.
            ScenarioHeadlineSnapshot snapshot = service.BuildHeadlineSnapshot("The First Ledger", 0, 9, SaloonGoalId);
            Assert.IsTrue(snapshot.IsFailed);
            Assert.AreEqual(1, snapshot.FailedCount);
            Assert.AreEqual(SaloonDeadlineDay, snapshot.DeadlineDayIndex);
            Assert.AreEqual("the-first-ledger", snapshot.ScenarioId);
        }

        [Test]
        public void Failure_QualifierMetByDeadline_NeverFails()
        {
            int eventCount = 0;
            ScenarioRunStateEvents.ScenarioFailed += (_, _) => eventCount++;
            var service = new ScenarioObjectiveService();
            service.BeginScenario(ScenarioObjectiveService.FirstLedgerScenarioId);
            service.RegisterDatedQualifier(SaloonGoalId, SaloonDeadlineDay, () => true);
            Assert.IsFalse(service.Evaluate(SaloonDeadlineDay + 100));
            Assert.IsFalse(service.IsFailed);
            Assert.AreEqual(0, eventCount);
            ScenarioHeadlineSnapshot snapshot = service.BuildHeadlineSnapshot("The First Ledger", 1, 9, "prairie-crossing-ownership");
            Assert.IsFalse(snapshot.IsFailed);
            Assert.AreEqual(0, snapshot.FailedCount);
            Assert.AreEqual(-1, snapshot.DeadlineDayIndex, "unregistered next goal has no deadline");
        }
    }
}
